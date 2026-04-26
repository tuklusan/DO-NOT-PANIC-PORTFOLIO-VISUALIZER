using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;
using PortfolioSaver.Data.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class QuoteSchedulerTests
{
    [Fact]
    public async Task RefreshAsync_ReturnsQuotes()
    {
        QuoteScheduler scheduler = new(new FakeProvider(), new FakeCache(), new ProviderHealthService());
        var result = await scheduler.RefreshAsync(["AAPL"]);
        Assert.Single(result);
    }

    private sealed class FakeProvider : IQuoteProvider
    {
        public Task<IReadOnlyList<QuoteSnapshot>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<QuoteSnapshot>>([new QuoteSnapshot { Symbol = symbols.First() }]);

        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakeCache : IQuoteCacheService
    {
        public Task<IReadOnlyList<QuoteSnapshot>> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<QuoteSnapshot>>([]);

        public Task SaveAsync(IEnumerable<QuoteSnapshot> quotes, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
