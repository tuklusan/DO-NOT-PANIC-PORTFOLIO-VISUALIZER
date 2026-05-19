using YFinance.NET.Config;
using YFinance.NET.Features.History;
using YFinance.NET.Features.Quotes;
using YFinance.NET.Transport;

namespace YFinance.NET.Api;

public sealed class YFinanceClient : IDisposable
{
    // Keep the public composition surface close to upstream yfinance concepts so
    // future fork syncs have an obvious .NET landing zone.
    private readonly YahooFinanceHttpClient _httpClient;
    private readonly QuoteService _quoteService;
    private readonly QuoteSummaryService _quoteSummaryService;
    private readonly TickerInfoService _tickerInfoService;
    private readonly HistoryService _historyService;

    public YFinanceClient(YFinanceOptions? options = null)
    {
        YFinanceOptions resolvedOptions = options ?? new YFinanceOptions();
        _httpClient = new YahooFinanceHttpClient(resolvedOptions);
        _quoteService = new QuoteService(_httpClient, resolvedOptions);
        _quoteSummaryService = new QuoteSummaryService(_httpClient, resolvedOptions);
        _tickerInfoService = new TickerInfoService(_quoteService, _quoteSummaryService, resolvedOptions);
        _historyService = new HistoryService(_httpClient);
    }

    public Ticker Ticker(string symbol) => new(symbol, _quoteService, _quoteSummaryService, _tickerInfoService, _historyService);

    public Tickers Tickers(IEnumerable<string> symbols) => new(symbols, _quoteService, _quoteSummaryService, _tickerInfoService, _historyService);

    public void Dispose() => _httpClient.Dispose();
}
