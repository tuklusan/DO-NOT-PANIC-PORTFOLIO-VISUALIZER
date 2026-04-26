namespace PortfolioSaver.Core.Models;

public sealed class TickerItem
{
    public string Symbol { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public decimal? CostBasis { get; set; }
    public string Currency { get; set; } = "USD";
    public bool Enabled { get; set; } = true;
}
