namespace YFinance.NET.Protocol.Dtos;

public sealed record MarketTimingDto(
    string Symbol,
    string? ExchangeName,
    string? ExchangeTimezoneName,
    string? InstrumentType,
    DateTimeOffset? RegularMarketTimeUtc,
    long? GmtOffsetSeconds,
    CurrentTradingPeriodsDto? CurrentTradingPeriod,
    DateOnly ExchangeLocalDate,
    DateTimeOffset FetchedUtc,
    CacheMetadataDto Cache);
