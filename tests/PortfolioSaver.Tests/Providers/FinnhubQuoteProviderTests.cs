using PortfolioSaver.Data.Providers;
using Xunit;

namespace PortfolioSaver.Tests.Providers;

public sealed class FinnhubQuoteProviderTests
{
    [Fact]
    public async Task GetQuotesAsync_WithoutApiKey_ThrowsConfigurationError()
    {
        using HttpClient client = new();
        FinnhubQuoteProvider provider = new(client, string.Empty);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetQuotesAsync(["AAPL"]));
    }
}
