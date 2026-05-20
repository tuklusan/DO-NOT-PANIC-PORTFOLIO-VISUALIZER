namespace YFinance.NET.Models;

public sealed record CurrentTradingPeriods(
    TradingPeriodWindow? Pre,
    TradingPeriodWindow? Regular,
    TradingPeriodWindow? Post);
