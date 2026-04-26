using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;

namespace PortfolioSaver.Data.Services;

public sealed class QuoteScheduler
{
    private readonly IQuoteProvider _quoteProvider;
    private readonly IQuoteCacheService _quoteCacheService;
    private readonly ProviderHealthService _providerHealthService;

    public QuoteScheduler(IQuoteProvider quoteProvider, IQuoteCacheService quoteCacheService, ProviderHealthService providerHealthService)
    {
        _quoteProvider = quoteProvider;
        _quoteCacheService = quoteCacheService;
        _providerHealthService = providerHealthService;
    }

    public async Task<IReadOnlyList<QuoteSnapshot>> RefreshAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<QuoteSnapshot> quotes = await _quoteProvider.GetQuotesAsync(symbols, cancellationToken);
            await _quoteCacheService.SaveAsync(quotes, cancellationToken);
            _providerHealthService.MarkSuccess();
            return quotes;
        }
        catch (Exception ex)
        {
            _providerHealthService.MarkFailure(ex.Message);
            return await _quoteCacheService.LoadAsync(cancellationToken);
        }
    }
}
