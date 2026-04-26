using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Data.Interfaces;

public interface IQuoteCacheService
{
    Task SaveAsync(IEnumerable<QuoteSnapshot> quotes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QuoteSnapshot>> LoadAsync(CancellationToken cancellationToken = default);
}
