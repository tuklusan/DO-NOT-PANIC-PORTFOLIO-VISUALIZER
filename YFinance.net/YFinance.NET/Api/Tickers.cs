using YFinance.NET.Features.History;
using YFinance.NET.Features.Quotes;
using YFinance.NET.Models;

namespace YFinance.NET.Api;

public sealed class Tickers
{
    private static readonly string[] DefaultInfoModules = ["financialData", "quoteType", "defaultKeyStatistics", "assetProfile", "summaryDetail"];

    private readonly string[] _symbols;
    private readonly QuoteService _quoteService;
    private readonly QuoteSummaryService _quoteSummaryService;
    private readonly TickerInfoService _tickerInfoService;
    private readonly HistoryService _historyService;

    internal Tickers(IEnumerable<string> symbols, QuoteService quoteService, QuoteSummaryService quoteSummaryService, TickerInfoService tickerInfoService, HistoryService historyService)
    {
        _symbols = symbols.Select(static symbol => symbol.Trim().ToUpperInvariant())
                          .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
                          .Distinct(StringComparer.Ordinal)
                          .ToArray();
        _quoteService = quoteService;
        _quoteSummaryService = quoteSummaryService;
        _tickerInfoService = tickerInfoService;
        _historyService = historyService;
    }

    public IReadOnlyList<string> Symbols => _symbols;

    public IReadOnlyDictionary<string, Ticker> AsDictionary()
        => _symbols.ToDictionary(static symbol => symbol, symbol => new Ticker(symbol, _quoteService, _quoteSummaryService, _tickerInfoService, _historyService), StringComparer.Ordinal);

    public Task<IReadOnlyDictionary<string, QuoteSnapshot>> GetQuotesAsync(CancellationToken cancellationToken = default)
        => _quoteService.GetQuotesAsync(_symbols, cancellationToken);

    public Task<IReadOnlyDictionary<string, TickerInfo?>> GetInfosAsync(CancellationToken cancellationToken = default)
        => _tickerInfoService.GetInfosAsync(_symbols, cancellationToken);

    public async Task<IReadOnlyDictionary<string, QuoteSummaryResult?>> GetSummariesAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, QuoteSummaryResult?> results = new(StringComparer.OrdinalIgnoreCase);
        foreach (string symbol in _symbols)
        {
            results[symbol] = await _quoteSummaryService.GetSummaryAsync(symbol, DefaultInfoModules, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    public async Task<IReadOnlyDictionary<string, HistoryResponse>> GetHistoryResponsesAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, string interval = "1d", CancellationToken cancellationToken = default)
    {
        Dictionary<string, HistoryResponse> results = new(StringComparer.OrdinalIgnoreCase);
        foreach (string symbol in _symbols)
        {
            results[symbol] = await _historyService.GetHistoryResponseAsync(symbol, startUtc, endUtc, interval, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }
}