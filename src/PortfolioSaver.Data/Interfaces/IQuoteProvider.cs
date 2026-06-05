using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Data.Interfaces;

public interface IQuoteProvider
{
    /// <summary>
    /// Returns all successfully resolved quotes. Missing symbols are omitted; providers should throw only when no usable quote result is available or the request itself fails.
    /// </summary>
    Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
