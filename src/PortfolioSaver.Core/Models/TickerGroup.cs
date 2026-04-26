using PortfolioSaver.Core.Enums;

namespace PortfolioSaver.Core.Models;

public sealed class TickerGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Group";
    public RenderMode RenderMode { get; set; } = RenderMode.HorizontalTape;
    public ScrollDirection Direction { get; set; } = ScrollDirection.Left;
    public double Speed { get; set; } = 0.9;
    public double RowHeight { get; set; } = 56.0;
    public bool Enabled { get; set; } = true;
    public List<TickerItem> Tickers { get; set; } = [];
}
