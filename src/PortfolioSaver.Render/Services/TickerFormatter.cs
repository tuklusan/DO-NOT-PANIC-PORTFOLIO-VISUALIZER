using PortfolioSaver.Core.Models;

namespace PortfolioSaver.Render.Services;

public sealed class TickerFormatter
{
    public string Format(QuoteSnapshot quote, TickerItem? ticker = null)
    {
        string price = quote.Last?.ToString("0.##") ?? "--";
        string pct = quote.ChangePercent is decimal p ? $"{p:+0.##;-0.##;0}%" : "--";

        if (ticker?.Quantity is decimal quantity && quote.Last is decimal last)
        {
            decimal marketValue = quantity * last;
            return $"{quote.Symbol} {price} {pct} MV ${marketValue:N0}";
        }

        return $"{quote.Symbol} {price} {pct}";
    }
}
