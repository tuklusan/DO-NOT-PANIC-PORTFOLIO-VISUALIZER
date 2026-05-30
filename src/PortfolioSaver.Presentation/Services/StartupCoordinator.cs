using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Providers;
using PortfolioSaver.Data.Services;
using PortfolioSaver.Media.Services;
using PortfolioSaver.Render.Services;
using PortfolioSaver.Render.ViewModels;
using PortfolioSaver.Shared.Diagnostics;
using PortfolioSaver.Shared.Helpers;

namespace PortfolioSaver.Screensaver.Services;

public sealed class StartupCoordinator
{
    private const int MinimumTapeItemCount = 18;
    private const string StatusFreshnessAnchorSymbol = "^SPX";

    private readonly ScreensaverSettingsService _settingsService = new();
    private readonly ExchangePhotoCacheService _exchangePhotoCacheService = new();
    private readonly NetworkAvailabilityService _networkAvailabilityService = new();
    private readonly HistoricalGraphBuilder _historicalGraphBuilder = new();
    private readonly FloatingClockBuilder _floatingClockBuilder = new();
    private readonly FinanceNewsService _financeNewsService = new();
    private readonly SymbolProfileStore _symbolProfileStore = new(Path.Combine(PathHelper.GetLocalDataDirectory(), "symbol-profiles.json"));
    private readonly Dictionary<string, QuoteSnapshot> _runtimeQuoteMemory = new(StringComparer.OrdinalIgnoreCase);
    private int _sequentialRuntimeCursor;
    private string _sequentialRuntimeFingerprint = string.Empty;
    private readonly Func<bool> _isNetworkAvailable;
    private readonly Func<HttpClient, IQuoteProvider> _createYahooProvider;

    public StartupCoordinator(
        Func<bool>? networkAvailability = null,
        Func<HttpClient, IQuoteProvider>? yahooProviderFactory = null,
        Func<HttpClient, IQuoteProvider>? officialMacroProviderFactory = null,
        Func<HttpClient, IQuoteProvider>? globalMarketProviderFactory = null,
        ProviderBudgetLedgerService? providerBudgetLedgerService = null)
    {
        _isNetworkAvailable = networkAvailability ?? _networkAvailabilityService.IsNetworkAvailable;
        _createYahooProvider = yahooProviderFactory ?? (client => new YahooFinanceQuoteProvider(client));
    }

    public ScreensaverSceneState BuildBootstrapScene()
    {
        ConsumePendingRuntimeQuoteSeeds();
        AppSettings settings = _settingsService.Load();
        bool networkAvailable = _isNetworkAvailable();
        IReadOnlyList<string> backgroundPaths = _exchangePhotoCacheService.GetImmediateBackgrounds(settings);
        Dictionary<string, QuoteSnapshot> cachedQuotes = _runtimeQuoteMemory.ToDictionary(
            pair => pair.Key,
            pair => CloneQuote(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> headlines = _financeNewsService.GetCachedHeadlines(settings.NewsScrollerMode);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        bool showNetworkWaitingOverlay = !networkAvailable;

        TraceRuntimeState(
            "BootstrapSceneBuilt",
            new KeyValuePair<string, object?>("network_available", networkAvailable),
            new KeyValuePair<string, object?>("background_count", backgroundPaths.Count),
            new KeyValuePair<string, object?>("cached_quote_count", cachedQuotes.Count),
            new KeyValuePair<string, object?>("headline_count", headlines.Count),
            new KeyValuePair<string, object?>("group_count", settings.Groups.Count(group => group.Enabled)),
            new KeyValuePair<string, object?>("show_network_waiting_overlay", showNetworkWaitingOverlay));

        bool hasBootstrapUpdatedSymbol = TryGetLatestUpdatedSymbol(cachedQuotes, out string latestBootstrapSymbol, out DateTimeOffset latestBootstrapFetchUtc);

        return new ScreensaverSceneState
        {
            Settings = settings,
            Quotes = cachedQuotes,
            Tapes = BuildTapeViewModels(settings, cachedQuotes),
            News = BuildNews(headlines),
            Status = new StatusBarViewModel
            {
                MarketStatusText = "Market (New York): --",
                UpdatedPrefixText = "Last Updated:",
                UpdatedTickerFieldText = FormatUpdatedTickerField(
                    hasBootstrapUpdatedSymbol ? latestBootstrapSymbol : null,
                    hasBootstrapUpdatedSymbol && cachedQuotes.TryGetValue(latestBootstrapSymbol, out QuoteSnapshot? bootstrapQuote)
                        ? bootstrapQuote.ChangePercent
                        : null,
                    hasBootstrapUpdatedSymbol ? latestBootstrapFetchUtc : DateTimeOffset.MinValue),
                UpdatedTickerFieldForeground = ResolveUpdatedTickerFieldBrush(
                    hasBootstrapUpdatedSymbol && cachedQuotes.TryGetValue(latestBootstrapSymbol, out bootstrapQuote)
                        ? bootstrapQuote.ChangePercent
                        : null),
                ClockDateText = DateTimeOffset.UtcNow.ToString("ddd dd-MMM", CultureInfo.InvariantCulture).ToUpperInvariant(),
                ClockText = $"{DateTimeOffset.UtcNow:HH:mm} UTC"
            },
            Graphs = [],
            Clock = settings.EnableFloatingClock ? _floatingClockBuilder.BuildDefault() : null,
            BackgroundPaths = backgroundPaths,
            ShowNetworkWaitingOverlay = showNetworkWaitingOverlay,
            NetworkWaitingTitle = networkAvailable
                ? "Loading market data"
                : "Waiting for network",
            NetworkWaitingDetail = networkAvailable
                ? "Fetching live quotes, history, and exchange photos..."
                : $"Retrying live quotes and exchange photos every {FormatRefreshCadenceText(settings)}."
        };
    }

    public IReadOnlyList<TapeViewModel> BuildTapesForQuotes(AppSettings settings, IReadOnlyDictionary<string, QuoteSnapshot> quotes)
        => BuildTapeViewModels(settings, quotes);

    public async Task<ScreensaverSceneState> BuildSceneAsync(int graphRotationSeed = 0, CancellationToken cancellationToken = default)
    {
        ConsumePendingRuntimeQuoteSeeds();
        AppSettings settings = _settingsService.Load();
        bool networkAvailable = _isNetworkAvailable();
        using HttpClient httpClient = HttpClientFactory.Create(TimeSpan.FromSeconds(Math.Max(3, settings.HttpTimeoutSeconds)));
        IQuoteProvider yahooFinanceProvider = _createYahooProvider(httpClient);

        List<string> portfolioSymbols = BuildInterleavedPortfolioSymbols(settings);
        List<string> ancillarySymbols =
        [
            .. FloatingClockBuilder.GetWorldIndexSymbols(),
            .. GetMacroIndicatorSymbols()
        ];

        Task<Dictionary<string, QuoteSnapshot>> quotesTask = LoadQuotesAsync(
            portfolioSymbols,
            ancillarySymbols,
            settings,
            networkAvailable,
            yahooFinanceProvider,
            graphRotationSeed,
            cancellationToken);
        Task<IReadOnlyList<string>> backgroundsTask = _exchangePhotoCacheService.GetAvailableBackgroundsAsync(
            settings,
            httpClient,
            networkAvailable,
            cancellationToken);
        Task<IReadOnlyList<string>> headlinesTask = _financeNewsService.GetHeadlinesAsync(
            httpClient,
            settings,
            networkAvailable,
            cancellationToken);

        await Task.WhenAll(quotesTask, backgroundsTask, headlinesTask);

        Dictionary<string, QuoteSnapshot> quotes = await quotesTask;
        IReadOnlyList<string> backgroundPaths = await backgroundsTask;
        IReadOnlyList<string> headlines = await headlinesTask;

        return BuildSceneState(settings, quotes, backgroundPaths, headlines, networkAvailable);
    }

    public async Task<ScreensaverSceneState> BuildProgressiveQuoteSceneAsync(int graphRotationSeed = 0, CancellationToken cancellationToken = default)
    {
        ConsumePendingRuntimeQuoteSeeds();
        AppSettings settings = _settingsService.Load();
        bool networkAvailable = _isNetworkAvailable();
        using HttpClient httpClient = HttpClientFactory.Create(TimeSpan.FromSeconds(Math.Max(3, settings.HttpTimeoutSeconds)));
        IQuoteProvider yahooFinanceProvider = _createYahooProvider(httpClient);

        List<string> portfolioSymbols = BuildInterleavedPortfolioSymbols(settings);
        List<string> ancillarySymbols =
        [
            .. FloatingClockBuilder.GetWorldIndexSymbols(),
            .. GetMacroIndicatorSymbols()
        ];

        Dictionary<string, QuoteSnapshot> quotes = await LoadQuotesAsync(
            portfolioSymbols,
            ancillarySymbols,
            settings,
            networkAvailable,
            yahooFinanceProvider,
            graphRotationSeed,
            cancellationToken);

        IReadOnlyList<string> backgroundPaths = _exchangePhotoCacheService.GetImmediateBackgrounds(settings);
        IReadOnlyList<string> headlines = _financeNewsService.GetCachedHeadlines(settings.NewsScrollerMode);

        return BuildSceneState(settings, quotes, backgroundPaths, headlines, networkAvailable);
    }

    public void PrimeRuntimeQuotes(IReadOnlyDictionary<string, QuoteSnapshot> quotes)
    {
        foreach ((string symbol, QuoteSnapshot quote) in quotes)
            _runtimeQuoteMemory[symbol] = CloneQuote(quote);
    }

    public async IAsyncEnumerable<FloatingGraphViewModel> LoadGraphsIncrementallyAsync(
        AppSettings settings,
        int graphRotationSeed,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const int graphLookbackDays = 1;

        if (!settings.EnableFloatingGraphs)
            yield break;

        bool networkAvailable = _isNetworkAvailable();
        IReadOnlyDictionary<string, SymbolProfile> symbolProfiles = _symbolProfileStore.Load();
        using HttpClient httpClient = HttpClientFactory.Create(TimeSpan.FromSeconds(Math.Max(3, settings.HttpTimeoutSeconds)));
        IHistoricalCacheService cacheService = new HistoricalCacheService(settings.HistoricalCacheRootFolder);
        IHistoricalDataProvider historicalProvider = new HybridHistoricalDataProvider(
            cacheService,
            httpClient,
            TimeSpan.FromHours(Math.Max(1, settings.HistoricalRefreshHours)),
            graphRotationSeed,
            symbolProfiles);

        List<(TickerGroup Group, TickerItem Ticker)> graphPairs = SelectGraphTickerPairs(settings, graphRotationSeed).ToList();
        Dictionary<string, TickerHistorySnapshot> cachedBySymbol = new(StringComparer.OrdinalIgnoreCase);
        List<string> liveFetchSymbols = [];

        foreach ((TickerGroup group, TickerItem ticker) in graphPairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TraceGraph($"Graph warmup checking {ticker.Symbol} on {group.Name}.");

            TickerHistorySnapshot? cached = await cacheService.LoadAsync(ticker.Symbol, cancellationToken);
            if (cached is not null && cached.LookbackDays == graphLookbackDays && cached.Points.Count >= 2)
            {
                cachedBySymbol[ticker.Symbol] = cached;
                TraceGraph($"Graph warmup using cache for {ticker.Symbol} with {cached.Points.Count} points.");
                yield return BuildGraph(group.Name, cached, settings);
            }

            if (!networkAvailable)
            {
                TraceGraph($"Graph warmup skipped live fetch for {ticker.Symbol} because network is unavailable.");
                continue;
            }

            if (!HasEnabledHistorySource(ticker.Symbol))
            {
                TraceGraph($"Graph warmup skipped live fetch for {ticker.Symbol} because no supported history source is known.");
                continue;
            }

            if (!liveFetchSymbols.Contains(ticker.Symbol, StringComparer.OrdinalIgnoreCase))
                liveFetchSymbols.Add(ticker.Symbol);
        }

        if (!networkAvailable || liveFetchSymbols.Count == 0)
            yield break;

        IReadOnlyList<TickerHistorySnapshot> refreshedSnapshots = await historicalProvider.GetHistoryAsync(
            liveFetchSymbols,
            graphLookbackDays,
            cancellationToken);
        Dictionary<string, TickerHistorySnapshot> refreshedBySymbol = refreshedSnapshots
            .Where(snapshot => snapshot.Points.Count >= 2)
            .GroupBy(snapshot => snapshot.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToDictionary(snapshot => snapshot.Symbol, StringComparer.OrdinalIgnoreCase);

        foreach ((TickerGroup group, TickerItem ticker) in graphPairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!refreshedBySymbol.TryGetValue(ticker.Symbol, out TickerHistorySnapshot? refreshed))
            {
                TraceGraph($"Graph warmup received insufficient live history for {ticker.Symbol}.");
                continue;
            }

            if (cachedBySymbol.TryGetValue(ticker.Symbol, out TickerHistorySnapshot? cached) &&
                SnapshotsEquivalent(cached, refreshed))
            {
                TraceGraph($"Graph warmup live history for {ticker.Symbol} matched cache.");
                continue;
            }

            TraceGraph($"Graph warmup fetched live history for {ticker.Symbol} with {refreshed.Points.Count} points.");
            yield return BuildGraph(group.Name, refreshed, settings);
        }
    }

    private async Task<Dictionary<string, QuoteSnapshot>> LoadQuotesAsync(
        IReadOnlyList<string> portfolioSymbols,
        IReadOnlyList<string> benchmarkSymbols,
        AppSettings settings,
        bool networkAvailable,
        IQuoteProvider yahooFinanceProvider,
        int refreshSeed,
        CancellationToken cancellationToken)
    {
        List<string> orderedSymbols =
        [
            .. portfolioSymbols,
            .. benchmarkSymbols
        ];
        orderedSymbols = orderedSymbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Dictionary<string, QuoteSnapshot> cachedQuotes = _runtimeQuoteMemory.ToDictionary(
            pair => pair.Key,
            pair => CloneQuote(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        if (!networkAvailable)
            TraceRuntime($"Network probe unavailable. Attempting opportunistic live quote refresh. Cached={cachedQuotes.Count}");

        Dictionary<string, QuoteSnapshot> results = cachedQuotes.ToDictionary(
            pair => pair.Key,
            pair => CloneQuote(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        string? latestUpdatedSymbol = null;
        DateTimeOffset latestUpdatedFetchUtc = DateTimeOffset.MinValue;

        TraceRuntimeState(
            "QuoteRefreshPlan",
            new KeyValuePair<string, object?>("ordered_symbol_count", orderedSymbols.Count),
            new KeyValuePair<string, object?>("cached_quote_count", cachedQuotes.Count),
            new KeyValuePair<string, object?>("network_available", networkAvailable),
            new KeyValuePair<string, object?>("polling_interval_seconds", QuoteRefreshPolicy.GetRefreshPollingInterval(settings, nowUtc).TotalSeconds),
            new KeyValuePair<string, object?>("ordered_symbols", PreviewSymbols(orderedSymbols)));

        if (!networkAvailable || orderedSymbols.Count == 0)
        {
            TraceRuntimeState(
                "QuoteRefreshSkipped",
                new KeyValuePair<string, object?>("reason", networkAvailable ? "no_symbols_configured" : "network_unavailable"),
                new KeyValuePair<string, object?>("result_quote_count", results.Count));
            PrimeRuntimeQuotes(results);
            return results;
        }

        List<string> requestSymbols = TakeSequentialRequestSymbols(orderedSymbols);
        if (requestSymbols.Count > 0)
        {
            try
            {
                TraceRuntime($"Requesting YFinance.NET sequential quote for [{string.Join(", ", requestSymbols)}]");
                IReadOnlyList<QuoteSnapshot> fetched = await yahooFinanceProvider.GetQuotesAsync(requestSymbols, cancellationToken);
                if (fetched.Count == 0)
                {
                    TraceRuntime($"YFinance.NET returned no quotes for [{string.Join(", ", requestSymbols)}]");
                }
                TraceRuntimeState(
                    "SequentialQuoteReturned",
                    new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(requestSymbols)),
                    new KeyValuePair<string, object?>("fetched_symbols", PreviewSymbols(fetched.Select(quote => quote.Symbol))));

                foreach (QuoteSnapshot quote in fetched)
                {
                    results[quote.Symbol] = quote;
                    NoteLatestUpdatedQuote(quote, ref latestUpdatedSymbol, ref latestUpdatedFetchUtc);
                }
            }
            catch (Exception ex)
            {
                if (TryGetPartialQuotes(ex, out IReadOnlyList<QuoteSnapshot>? partialQuotes))
                {
                    foreach (QuoteSnapshot quote in partialQuotes!)
                    {
                        results[quote.Symbol] = quote;
                        NoteLatestUpdatedQuote(quote, ref latestUpdatedSymbol, ref latestUpdatedFetchUtc);
                    }
                }

                TraceRuntime($"YFinance.NET sequential quote failed for [{string.Join(", ", requestSymbols)}]: {ex.GetType().Name}: {ex.Message}");
                TraceRuntimeState(
                    "SequentialQuoteFailed",
                    new KeyValuePair<string, object?>("requested_symbols", PreviewSymbols(requestSymbols)),
                    new KeyValuePair<string, object?>("is_rate_limited", IsRateLimited(ex)));
            }
        }

        foreach (string symbol in orderedSymbols)
        {
            if (results.ContainsKey(symbol))
                continue;

            if (cachedQuotes.TryGetValue(symbol, out QuoteSnapshot? cached))
                results[symbol] = CloneQuote(cached);
        }

        TraceRuntimeState(
            "QuoteResolutionSummary",
            new KeyValuePair<string, object?>("result_quote_count", results.Count),
            new KeyValuePair<string, object?>("refreshed_symbol_count", requestSymbols.Count),
            new KeyValuePair<string, object?>("stale_symbol_count", results.Values.Count(quote => quote.IsStale)),
            new KeyValuePair<string, object?>("stale_symbols", PreviewSymbols(results.Values.Where(quote => quote.IsStale).Select(quote => quote.Symbol))),
            new KeyValuePair<string, object?>("missing_value_symbols", PreviewSymbols(results.Values.Where(quote => !quote.Last.HasValue && !quote.PreviousClose.HasValue).Select(quote => quote.Symbol))),
            new KeyValuePair<string, object?>("remaining_symbol_count", Math.Max(0, orderedSymbols.Count - requestSymbols.Count)),
            new KeyValuePair<string, object?>("remaining_symbols", PreviewSymbols(orderedSymbols.Except(requestSymbols, StringComparer.OrdinalIgnoreCase))),
            new KeyValuePair<string, object?>("macro_missing_symbols", PreviewMissingSymbols(GetMacroIndicatorSymbols(), results, settings)),
            new KeyValuePair<string, object?>("world_index_missing_symbols", PreviewMissingSymbols(FloatingClockBuilder.GetWorldIndexSymbols(), results, settings)));
        TraceRuntime($"Quotes resolved. Refreshed={requestSymbols.Count} Cached={Math.Max(0, results.Count - requestSymbols.Count)} Remaining={Math.Max(0, orderedSymbols.Count - requestSymbols.Count)}");
        PrimeRuntimeQuotes(results);
        return results;
    }

    private static IReadOnlyList<TickerItem> SelectGraphTickers(TickerGroup group, AppSettings settings, int rotationSeed)
    {
        List<TickerItem> enabledTickers = group.Tickers
            .Where(ticker => ticker.Enabled)
            .ToList();

        if (enabledTickers.Count == 0)
            return [];

        int visibleCount = settings.MaxFloatingGraphsPerTape <= 0
            ? enabledTickers.Count
            : Math.Min(enabledTickers.Count, Math.Max(1, settings.MaxFloatingGraphsPerTape));

        if (visibleCount >= enabledTickers.Count)
            return enabledTickers;

        int normalizedSeed = Math.Abs(rotationSeed);
        int startIndex = normalizedSeed % enabledTickers.Count;
        List<TickerItem> selected = [];
        for (int i = 0; i < visibleCount; i++)
            selected.Add(enabledTickers[(startIndex + i) % enabledTickers.Count]);

        return selected;
    }

    private static IReadOnlyList<(TickerGroup Group, TickerItem Ticker)> SelectGraphTickerPairs(AppSettings settings, int graphRotationSeed)
    {
        const int maxSceneGraphCards = 12;
        List<(TickerGroup Group, List<TickerItem> Tickers)> groupSelections = [];
        int groupIndex = 0;
        foreach (TickerGroup group in settings.Groups.Where(group => group.Enabled))
        {
            groupSelections.Add((group, SelectGraphTickers(group, settings, graphRotationSeed + groupIndex).ToList()));
            groupIndex++;
        }

        List<(TickerGroup Group, TickerItem Ticker)> pairs = [];
        int maxTickerCount = groupSelections.Count == 0 ? 0 : groupSelections.Max(selection => selection.Tickers.Count);
        for (int tickerIndex = 0; tickerIndex < maxTickerCount; tickerIndex++)
        {
            foreach ((TickerGroup group, List<TickerItem> tickers) in groupSelections)
            {
                if (tickerIndex >= tickers.Count)
                    continue;

                pairs.Add((group, tickers[tickerIndex]));
            }
        }

        return pairs.Take(maxSceneGraphCards).ToList();
    }

    private static bool HasEnabledHistorySource(string symbol)
    {
        return !string.IsNullOrWhiteSpace(SymbolProfileHeuristics.Normalize(symbol));
    }

    private static List<string> BuildInterleavedPortfolioSymbols(AppSettings settings)
    {
        List<List<string>> groups = settings.Groups
            .Where(group => group.Enabled)
            .Select(group => group.Tickers
                .Where(ticker => ticker.Enabled)
                .Select(ticker => ticker.Symbol)
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .ToList())
            .Where(symbols => symbols.Count > 0)
            .ToList();

        if (groups.Count == 0)
            return [];

        List<string> ordered = [];
        int maxTickerCount = groups.Max(group => group.Count);
        for (int tickerIndex = 0; tickerIndex < maxTickerCount; tickerIndex++)
        {
            foreach (List<string> groupSymbols in groups)
            {
                if (tickerIndex < groupSymbols.Count)
                    ordered.Add(groupSymbols[tickerIndex]);
            }
        }

        return ordered;
    }

    public static IReadOnlyList<string> GetMacroIndicatorSymbols()
        => ["^VIX", "^IXIC", "^TNX", "^IRX", "GC=F", "BZ=F", "DX-Y.NYB", "BTC-USD"];

    private List<TapeViewModel> BuildTapeViewModels(AppSettings settings, IReadOnlyDictionary<string, QuoteSnapshot> quotes)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        List<TapeViewModel> tapes = [];
        foreach (TickerGroup group in settings.Groups.Where(group => group.Enabled))
        {
            List<TickerItem> activeTickers = group.Tickers
                .Where(ticker => ticker.Enabled && !string.IsNullOrWhiteSpace(ticker.Symbol))
                .ToList();
            if (activeTickers.Count == 0)
                continue;

            TapeViewModel tape = new()
            {
                Title = string.IsNullOrWhiteSpace(group.Name) ? "Tape" : group.Name.Trim(),
                Speed = group.Speed,
                Direction = group.Direction
            };

            foreach (TickerItem ticker in activeTickers)
            {
                quotes.TryGetValue(ticker.Symbol, out QuoteSnapshot? quote);
                tape.Items.Add(BuildTapeItem(ticker.Symbol, quote, ticker.DisplayName, settings, nowUtc));
            }

            ExpandTapeItems(tape.Items, MinimumTapeItemCount);
            tapes.Add(tape);
        }

        return tapes;
    }

    private static NewsFlasherViewModel BuildNews(IReadOnlyList<string> headlines)
    {
        NewsFlasherViewModel news = new()
        {
            Title = "FINANCE NEWS",
            Speed = Defaults.DefaultNewsSpeed
        };

        foreach (string headline in headlines.Where(headline => !string.IsNullOrWhiteSpace(headline)))
        {
            if (!FinanceNewsService.TryParseSpecialHeadline(headline, out string parsedText, out bool isSupplemental))
                continue;

            news.Headlines.Add(new NewsHeadlineViewModel
            {
                Text = parsedText,
                Foreground = isSupplemental ? Brushes.LightSteelBlue : Brushes.WhiteSmoke,
                IsSupplemental = isSupplemental
            });
        }

        if (news.Headlines.Count == 0)
            news.Headlines.Add(new NewsHeadlineViewModel { Text = "Waiting for summarized financial news..." });

        return news;
    }

    private static TapeItemViewModel BuildTapeItem(string symbol, QuoteSnapshot? quote, string displayName, AppSettings settings, DateTimeOffset nowUtc)
    {
        decimal? last = quote?.Last ?? quote?.PreviousClose;
        decimal? percent = quote?.ChangePercent;
        bool hasUsableValue = last is not null;
        bool isMissing = !hasUsableValue;
        string lastText = last is decimal lastValue
            ? lastValue.ToString("0.00", CultureInfo.InvariantCulture)
            : string.Empty;
        string percentText = percent is decimal percentValue
            ? $"{(percentValue >= 0 ? "+" : string.Empty)}{percentValue:0.00}%"
            : string.Empty;
        Brush changeBrush = percent switch
        {
            > 0 => Brushes.LimeGreen,
            < 0 => Brushes.OrangeRed,
            _ => Brushes.Gainsboro
        };

        return new TapeItemViewModel
        {
            SymbolText = symbol,
            LastText = lastText,
            ChangeText = percentText,
            IsWaitingOnData = isMissing,
            HasMissingData = isMissing,
            WaitingGlyphText = isMissing ? "◌" : "🕒",
            WaitingGlyphForeground = isMissing ? Brushes.DarkOrange : Brushes.Goldenrod,
            SymbolForeground = isMissing ? Brushes.DarkOrange : changeBrush,
            LastForeground = Brushes.WhiteSmoke,
            ChangeForeground = changeBrush,
            QuoteUpdateToken = quote?.FetchTimestampUtc.UtcTicks ?? 0
        };
    }


    private FloatingGraphViewModel BuildGraph(string tapeName, TickerHistorySnapshot snapshot, AppSettings settings)
    {
        FloatingGraphViewModel graph = _historicalGraphBuilder.Build(tapeName, snapshot, new Size(132, 40));
        graph.Width = 186;
        graph.Height = 78;
        graph.PlotWidth = 132;
        graph.PlotHeight = 40;
        graph.BounceWithinViewport = settings.EnableBouncingGraphCards;
        return graph;
    }

    private static bool SnapshotsEquivalent(TickerHistorySnapshot left, TickerHistorySnapshot right)
    {
        if (left.Points.Count != right.Points.Count)
            return false;

        if (left.Points.Count == 0)
            return true;

        HistoricalPricePoint leftLast = left.Points[^1];
        HistoricalPricePoint rightLast = right.Points[^1];
        return leftLast.TimestampUtc == rightLast.TimestampUtc && leftLast.Close == rightLast.Close;
    }

    private static List<string> RotateSymbols(IEnumerable<string> symbols, int refreshSeed)
    {
        List<string> distinctSymbols = symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctSymbols.Count <= 1)
            return distinctSymbols;

        int normalizedSeed = Math.Abs(refreshSeed) % distinctSymbols.Count;
        if (normalizedSeed == 0)
            return distinctSymbols;

        return
        [
            .. distinctSymbols.Skip(normalizedSeed),
            .. distinctSymbols.Take(normalizedSeed)
        ];
    }





    private static string FormatRefreshCadenceText(AppSettings settings)
    {
        TimeSpan cadence = QuoteRefreshPolicy.GetRefreshPollingInterval(settings, DateTimeOffset.UtcNow);
        return cadence < TimeSpan.FromSeconds(1)
            ? $"{cadence.TotalMilliseconds:0} ms"
            : $"{cadence.TotalSeconds:0.##} seconds";
    }

    private static bool HasUsableQuote(QuoteSnapshot? quote)
        => quote is not null &&
           ((quote.Last is decimal last && last > 0) ||
            (quote.PreviousClose is decimal previousClose && previousClose > 0));

    private List<string> TakeSequentialRequestSymbols(IReadOnlyList<string> orderedSymbols)
    {
        if (orderedSymbols.Count == 0)
            return [];

        List<string> sequenceSymbols = orderedSymbols
            .Where(symbol => !string.IsNullOrWhiteSpace(SymbolProfileHeuristics.Normalize(symbol)))
            .ToList();

        if (sequenceSymbols.Count == 0)
            return [];

        string fingerprint = string.Join("|", sequenceSymbols);
        if (!string.Equals(_sequentialRuntimeFingerprint, fingerprint, StringComparison.Ordinal))
        {
            _sequentialRuntimeFingerprint = fingerprint;
            _sequentialRuntimeCursor = 0;
        }

        if (_sequentialRuntimeCursor >= sequenceSymbols.Count)
            _sequentialRuntimeCursor = 0;

        string selected = sequenceSymbols[_sequentialRuntimeCursor];
        _sequentialRuntimeCursor = (_sequentialRuntimeCursor + 1) % sequenceSymbols.Count;
        return [selected];
    }



    private static bool IsRateLimited(Exception ex)
        => ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } ||
           ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("API credits", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("current minute", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetPartialQuotes(Exception ex, out IReadOnlyList<QuoteSnapshot>? partialQuotes)
    {
        if (ex is PartialQuoteResultException partial && partial.PartialQuotes.Count > 0)
        {
            partialQuotes = partial.PartialQuotes;
            return true;
        }

        partialQuotes = null;
        return false;
    }


    private static QuoteSnapshot CloneQuote(QuoteSnapshot source)
        => new()
        {
            Symbol = source.Symbol,
            Last = source.Last,
            Change = source.Change,
            ChangePercent = source.ChangePercent,
            PreviousClose = source.PreviousClose,
            Currency = source.Currency,
            MarketSession = source.MarketSession,
            ProviderTimestampUtc = source.ProviderTimestampUtc,
            FetchTimestampUtc = source.FetchTimestampUtc,
            IsStale = source.IsStale
        };

    private static void NoteLatestUpdatedQuote(
        QuoteSnapshot quote,
        ref string? latestUpdatedSymbol,
        ref DateTimeOffset latestUpdatedFetchUtc)
    {
        if (quote.FetchTimestampUtc < latestUpdatedFetchUtc)
            return;

        latestUpdatedFetchUtc = quote.FetchTimestampUtc;
        latestUpdatedSymbol = quote.Symbol;
    }

    private static void ExpandTapeItems(ICollection<TapeItemViewModel> items, int minimumCount)
    {
        if (items.Count == 0 || items.Count >= minimumCount)
            return;

        List<TapeItemViewModel> source = items.ToList();
        int cycleIndex = 0;

        while (items.Count < minimumCount)
        {
            int rotationOffset = CalculateTapeRepeatRotationOffset(source.Count, cycleIndex);
            foreach (TapeItemViewModel item in EnumerateTapeCycle(source, rotationOffset, items.LastOrDefault()?.SymbolText))
            {
                items.Add(CloneTapeItem(item));
                if (items.Count >= minimumCount)
                    break;
            }

            cycleIndex++;
        }
    }

    private static IEnumerable<TapeItemViewModel> EnumerateTapeCycle(
        IReadOnlyList<TapeItemViewModel> source,
        int rotationOffset,
        string? priorSymbol)
    {
        if (source.Count == 0)
            yield break;

        int startIndex = Math.Clamp(rotationOffset, 0, Math.Max(0, source.Count - 1));
        bool firstItem = true;
        for (int index = 0; index < source.Count; index++)
        {
            TapeItemViewModel next = source[(startIndex + index) % source.Count];
            if (firstItem &&
                !string.IsNullOrWhiteSpace(priorSymbol) &&
                source.Count > 1 &&
                string.Equals(next.SymbolText, priorSymbol, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            firstItem = false;
            yield return next;
        }
    }

    private static int CalculateTapeRepeatRotationOffset(int itemCount, int cycleIndex)
    {
        if (itemCount <= 2)
            return cycleIndex % Math.Max(1, itemCount);

        int step = Math.Max(1, itemCount / 2);
        return (cycleIndex * step) % itemCount;
    }

    private static TapeItemViewModel CloneTapeItem(TapeItemViewModel item) => new()
    {
        SymbolText = item.SymbolText,
        LastText = item.LastText,
        ChangeText = item.ChangeText,
        IsWaitingOnData = item.IsWaitingOnData,
        HasMissingData = item.HasMissingData,
        WaitingGlyphText = item.WaitingGlyphText,
        WaitingGlyphForeground = item.WaitingGlyphForeground,
        SymbolForeground = item.SymbolForeground,
        LastForeground = item.LastForeground,
        ChangeForeground = item.ChangeForeground
    };

    private static void TraceGraph(string message)
    {
        TraceLog.Info("StartupCoordinator.Graph", message);
    }

    private static void TraceRuntimeState(string eventName, params KeyValuePair<string, object?>[] fields)
    {
        TraceLog.InfoState("StartupCoordinator.Runtime", eventName, fields);
    }

    private static void TraceRuntime(string message)
    {
        TraceLog.Info("StartupCoordinator.Runtime", message);
    }


    private static IReadOnlyList<string> PreviewSymbols(IEnumerable<string> symbols, int maxCount = 10)
    {
        List<string> distinct = symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToList();

        return distinct;
    }

    private static IReadOnlyList<string> PreviewMissingSymbols(
        IEnumerable<string> expectedSymbols,
        IReadOnlyDictionary<string, QuoteSnapshot> quotes,
        AppSettings settings)
    {
        return expectedSymbols
            .Where(symbol =>
                !quotes.TryGetValue(symbol, out QuoteSnapshot? quote) ||
                (quote.Last is null && quote.PreviousClose is null))
            .Take(10)
            .ToList();
    }

    private static string FormatStatusBandText(string statusLine)
        => string.IsNullOrWhiteSpace(statusLine)
            ? "Market (New York): --"
            : statusLine.Replace(" | ", Environment.NewLine, StringComparison.Ordinal);




    public static bool TryGetStatusFreshnessAnchorFetchUtc(
        IReadOnlyDictionary<string, QuoteSnapshot> quotes,
        out DateTimeOffset fetchUtc)
    {
        fetchUtc = DateTimeOffset.MinValue;
        if (quotes.Count == 0)
            return false;

        if (quotes.TryGetValue(StatusFreshnessAnchorSymbol, out QuoteSnapshot? spxQuote) &&
            spxQuote.FetchTimestampUtc > DateTimeOffset.MinValue)
        {
            fetchUtc = spxQuote.FetchTimestampUtc;
            return true;
        }

        fetchUtc = quotes.Values
            .Select(quote => quote.FetchTimestampUtc)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();

        return fetchUtc > DateTimeOffset.MinValue;
    }

    public static bool TryGetLatestUpdatedSymbol(
        IReadOnlyDictionary<string, QuoteSnapshot> quotes,
        out string symbol,
        out DateTimeOffset fetchUtc)
    {
        symbol = string.Empty;
        fetchUtc = DateTimeOffset.MinValue;
        if (quotes.Count == 0)
            return false;

        (string Symbol, DateTimeOffset FetchUtc) latest = quotes
            .Where(pair => pair.Value.FetchTimestampUtc > DateTimeOffset.MinValue)
            .Select(pair => (Symbol: pair.Key, FetchUtc: pair.Value.FetchTimestampUtc))
            .OrderByDescending(pair => pair.FetchUtc)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(latest.Symbol) || latest.FetchUtc <= DateTimeOffset.MinValue)
            return false;

        symbol = latest.Symbol;
        fetchUtc = latest.FetchUtc;
        return true;
    }










    private ScreensaverSceneState BuildSceneState(
        AppSettings settings,
        IReadOnlyDictionary<string, QuoteSnapshot> quotes,
        IReadOnlyList<string> backgroundPaths,
        IReadOnlyList<string> headlines,
        bool networkAvailable)
    {
        List<TapeViewModel> tapes = BuildTapeViewModels(settings, quotes);
        NewsFlasherViewModel news = BuildNews(headlines);
        FloatingClockViewModel? clock = settings.EnableFloatingClock ? _floatingClockBuilder.BuildDefault() : null;

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        bool hasLatestUpdatedSymbol = TryGetLatestUpdatedSymbol(quotes, out string latestUpdatedSymbol, out DateTimeOffset latestUpdatedFetchUtc);
        DateTimeOffset lastUpdate = hasLatestUpdatedSymbol
            ? latestUpdatedFetchUtc
            : (TryGetStatusFreshnessAnchorFetchUtc(quotes, out DateTimeOffset anchorQuoteFetchUtc)
                ? anchorQuoteFetchUtc
                : nowUtc);
        StatusBarViewModel status = new()
        {
            MarketStatusText = "Market (New York): --",
            UpdatedPrefixText = "Last Updated:",
            UpdatedTickerFieldText = FormatUpdatedTickerField(hasLatestUpdatedSymbol ? latestUpdatedSymbol : null, hasLatestUpdatedSymbol ? quotes.GetValueOrDefault(latestUpdatedSymbol)?.ChangePercent : null, lastUpdate),
            UpdatedTickerFieldForeground = ResolveUpdatedTickerFieldBrush(hasLatestUpdatedSymbol ? quotes.GetValueOrDefault(latestUpdatedSymbol)?.ChangePercent : null),
            ClockDateText = nowUtc.ToString("ddd dd-MMM", CultureInfo.InvariantCulture).ToUpperInvariant(),
            ClockText = $"{nowUtc:HH:mm} UTC"
        };

        bool showNetworkWaitingOverlay = !networkAvailable;

        return new ScreensaverSceneState
        {
            Settings = settings,
            Quotes = new Dictionary<string, QuoteSnapshot>(quotes, StringComparer.OrdinalIgnoreCase),
            Tapes = tapes,
            News = news,
            Status = status,
            Graphs = [],
            Clock = clock,
            BackgroundPaths = backgroundPaths,
            ShowNetworkWaitingOverlay = showNetworkWaitingOverlay,
            NetworkWaitingTitle = "Waiting for network",
            NetworkWaitingDetail = $"Retrying live quotes and exchange photos every {FormatRefreshCadenceText(settings)}."
        };
    }

    private void ConsumePendingRuntimeQuoteSeeds()
    {
        foreach ((string symbol, QuoteSnapshot quote) in RuntimeQuoteSeedStore.ConsumeAll())
            _runtimeQuoteMemory[symbol] = CloneQuote(quote);
    }

    public static string FormatUpdatedTickerField(string? latestSymbol, decimal? changePercent, DateTimeOffset fetchUtc)
    {
        string age = fetchUtc > DateTimeOffset.MinValue
            ? TimeFormatHelper.ToAgeString(fetchUtc)
            : "--";
        string percentText = changePercent is decimal percent
            ? $"{(percent >= 0m ? "+" : string.Empty)}{percent:0.00}%"
            : string.Empty;
        string content = string.IsNullOrWhiteSpace(latestSymbol)
            ? age
            : string.IsNullOrWhiteSpace(percentText)
                ? $"{latestSymbol} {age}"
                : $"{latestSymbol} {percentText} {age}";

        return content.Length >= 25
            ? content[..25]
            : content.PadRight(25);
    }

    public static Brush ResolveUpdatedTickerFieldBrush(decimal? changePercent)
        => changePercent switch
        {
            > 0m => Brushes.LimeGreen,
            < 0m => Brushes.OrangeRed,
            _ => Brushes.Gainsboro
        };
}


