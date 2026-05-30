namespace YFinance.NET.Protocol.Dtos;

public sealed record TradingPeriodWindowDto(DateTimeOffset StartUtc, DateTimeOffset EndUtc, long? GmtOffsetSeconds);
