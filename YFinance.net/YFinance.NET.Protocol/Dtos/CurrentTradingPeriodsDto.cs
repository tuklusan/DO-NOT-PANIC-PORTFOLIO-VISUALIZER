namespace YFinance.NET.Protocol.Dtos;

public sealed record CurrentTradingPeriodsDto(
    TradingPeriodWindowDto? Pre,
    TradingPeriodWindowDto? Regular,
    TradingPeriodWindowDto? Post);
