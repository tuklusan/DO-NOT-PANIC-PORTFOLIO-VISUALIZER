namespace PortfolioSaver.Core.Models;

public sealed class TickerHistorySnapshot
{
    public string Symbol { get; set; } = string.Empty;
    public DateTimeOffset FetchTimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public int LookbackDays { get; set; } = 14;
    public List<HistoricalPricePoint> Points { get; set; } = [];

    public bool IsFresh(TimeSpan maxAge) => DateTimeOffset.UtcNow - FetchTimestampUtc <= maxAge;
}
