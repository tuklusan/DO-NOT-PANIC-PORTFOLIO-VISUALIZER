using YFinance.NET.Config;
using YFinance.NET.Features.History;
using YFinance.NET.Features.Quotes;
using YFinance.NET.Transport;

namespace YFinance.NET.Api;

public sealed class YFinanceClient : IDisposable
{
    private readonly YahooFinanceHttpClient _httpClient;
    private readonly QuoteService _quoteService;
    private readonly QuoteSummaryService _quoteSummaryService;
    private readonly HistoryService _historyService;

    public YFinanceClient(YFinanceOptions? options = null)
    {
        _httpClient = new YahooFinanceHttpClient(options);
        _quoteService = new QuoteService(_httpClient);
        _quoteSummaryService = new QuoteSummaryService(_httpClient);
        _historyService = new HistoryService(_httpClient);
    }

    public Ticker Ticker(string symbol) => new(symbol, _quoteService, _quoteSummaryService, _historyService);

    public Tickers Tickers(IEnumerable<string> symbols) => new(symbols, _quoteService, _quoteSummaryService, _historyService);

    public void Dispose() => _httpClient.Dispose();
}
