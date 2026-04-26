using PortfolioSaver.Core.Enums;
using PortfolioSaver.Core.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class MarketSessionResolverTests
{
    [Fact]
    public void Resolve_ReturnsAValidEnum()
    {
        MarketSessionResolver resolver = new();
        MarketSession session = resolver.Resolve(DateTimeOffset.UtcNow);
        Assert.True(Enum.IsDefined(session));
    }
}
