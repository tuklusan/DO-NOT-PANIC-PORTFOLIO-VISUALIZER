using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Core.Models;

public sealed class QuoteSnapshot
{
    public string Symbol { get; set; } = string.Empty;
    public decimal? Last { get; set; }
    public decimal? Change { get; set; }
    public decimal? ChangePercent { get; set; }
    public decimal? PreviousClose { get; set; }
    public string Currency { get; set; } = "USD";
    public MarketSession MarketSession { get; set; } = MarketSession.Unknown;
    public DateTimeOffset? ProviderTimestampUtc { get; set; }
    public DateTimeOffset FetchTimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool IsStale { get; set; }
}
