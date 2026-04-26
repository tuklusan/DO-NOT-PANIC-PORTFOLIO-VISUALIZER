using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Data.Interfaces;

public interface IQuoteProvider
{
    Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
