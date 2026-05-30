namespace YFinance.NET.Protocol.Dtos;

public sealed record HistoryMetadataDto(
    string? ExchangeName,
    string? FullExchangeName,
    string? Currency,
    string? ExchangeTimezoneName,
    string? ExchangeTimezoneShortName,
    long? GmtOffsetSeconds,
    DateTimeOffset? RegularMarketTimeUtc,
    CurrentTradingPeriodsDto? CurrentTradingPeriod);
