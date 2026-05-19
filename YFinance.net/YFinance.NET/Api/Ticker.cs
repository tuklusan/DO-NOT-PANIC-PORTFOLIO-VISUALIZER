using YFinance.NET.Features.History;
using YFinance.NET.Features.Quotes;
using YFinance.NET.Models;

namespace YFinance.NET.Api;

public sealed class Ticker
{
    private static readonly string[] DefaultInfoModules = ["financialData", "quoteType", "defaultKeyStatistics", "assetProfile", "summaryDetail"];
    private readonly string _symbol;
    private readonly QuoteService _quoteService;
    private readonly QuoteSummaryService _quoteSummaryService;
    private readonly HistoryService _historyService;

    internal Ticker(string symbol, QuoteService quoteService, QuoteSummaryService quoteSummaryService, HistoryService historyService)
    {
        _symbol = symbol.Trim().ToUpperInvariant();
        _quoteService = quoteService;
        _quoteSummaryService = quoteSummaryService;
        _historyService = historyService;
    }

    public string Symbol => _symbol;

    public Task<QuoteSnapshot?> GetQuoteAsync(CancellationToken cancellationToken = default)
        => _quoteService.GetQuoteAsync(_symbol, cancellationToken);

    public Task<QuoteSummaryResult?> GetInfoAsync(CancellationToken cancellationToken = default)
        => _quoteSummaryService.GetSummaryAsync(_symbol, DefaultInfoModules, cancellationToken);

    public Task<IReadOnlyList<HistoricalBar>> GetHistoryAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, string interval = "1d", CancellationToken cancellationToken = default)
        => _historyService.GetHistoryAsync(_symbol, startUtc, endUtc, interval, cancellationToken);
}
