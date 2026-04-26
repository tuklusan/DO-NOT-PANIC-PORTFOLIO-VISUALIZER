using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Data.Interfaces;

public interface IHistoricalCacheService
{
    Task<TickerHistorySnapshot?> LoadAsync(string symbol, CancellationToken cancellationToken = default);
    Task SaveAsync(TickerHistorySnapshot snapshot, CancellationToken cancellationToken = default);
    Task PurgeExpiredAsync(CancellationToken cancellationToken = default);
}
