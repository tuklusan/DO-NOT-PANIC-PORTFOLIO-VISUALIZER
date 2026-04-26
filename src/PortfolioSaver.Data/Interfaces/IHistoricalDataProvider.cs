using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Data.Interfaces;

public interface IHistoricalDataProvider
{
    Task<IReadOnlyList<TickerHistorySnapshot>> GetHistoryAsync(
        IEnumerable<string> symbols,
        int lookbackDays,
        CancellationToken cancellationToken = default);
}
