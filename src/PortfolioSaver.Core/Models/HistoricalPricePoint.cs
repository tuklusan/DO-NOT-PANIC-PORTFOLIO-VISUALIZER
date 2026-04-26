namespace PortfolioSaver.Core.Models;

public sealed class HistoricalPricePoint
{
    public DateTimeOffset TimestampUtc { get; set; }
    public decimal Close { get; set; }
}
