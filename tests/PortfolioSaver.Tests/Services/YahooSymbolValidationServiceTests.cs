using System.Net;
using PortfolioSaver.Config.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class YahooSymbolValidationServiceTests
{
    [Fact]
    public async Task ValidateAsync_WhenYahooRateLimits_MarksSymbolsDeferredInsteadOfInvalid()
    {
        YahooSymbolValidationService service = new((symbols, _, _) =>
            throw new HttpRequestException("Too Many Requests", null, HttpStatusCode.TooManyRequests));

        YahooSymbolValidationResult result = await service.ValidateAsync(["AAPL", "MSFT"], 5);

        Assert.True(result.WasRateLimited);
        Assert.Empty(result.InvalidSymbols);
        Assert.Equal(["AAPL", "MSFT"], result.DeferredSymbols);
    }
}
