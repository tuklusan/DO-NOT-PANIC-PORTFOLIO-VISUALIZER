using YFinance.NET.Features.History;
using YFinance.NET.Features.Quotes;
using YFinance.NET.Models;

namespace YFinance.NET.Api;

public sealed class Tickers
{
    private readonly string[] _symbols;
    private readonly QuoteService _quoteService;
    private readonly QuoteSummaryService _quoteSummaryService;
    private readonly HistoryService _historyService;

    internal Tickers(IEnumerable<string> symbols, QuoteService quoteService, QuoteSummaryService quoteSummaryService, HistoryService historyService)
    {
        _symbols = symbols.Select(static symbol => symbol.Trim().ToUpperInvariant())
                          .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
                          .Distinct(StringComparer.Ordinal)
                          .ToArray();
        _quoteService = quoteService;
        _quoteSummaryService = quoteSummaryService;
        _historyService = historyService;
    }

    public IReadOnlyList<string> Symbols => _symbols;

    public IReadOnlyDictionary<string, Ticker> AsDictionary()
        => _symbols.ToDictionary(static symbol => symbol, symbol => new Ticker(symbol, _quoteService, _quoteSummaryService, _historyService), StringComparer.Ordinal);

    public Task<IReadOnlyDictionary<string, QuoteSnapshot>> GetQuotesAsync(CancellationToken cancellationToken = default)
        => _quoteService.GetQuotesAsync(_symbols, cancellationToken);

    public async Task<IReadOnlyDictionary<string, QuoteSummaryResult?>> GetInfosAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, QuoteSummaryResult?> results = new(StringComparer.OrdinalIgnoreCase);
        foreach (string symbol in _symbols)
        {
            results[symbol] = await _quoteSummaryService.GetSummaryAsync(
                symbol,
                ["financialData", "quoteType", "defaultKeyStatistics", "assetProfile", "summaryDetail"],
                cancellationToken).ConfigureAwait(false);
        }
        return results;
    }
}
