namespace YFinance.NET.Protocol.Dtos;

public sealed record TickerInfoDto(
    string Symbol,
    string? ShortName,
    string? LongName,
    string? DisplayName,
    string? Currency,
    string? Exchange,
    string? ExchangeTimezoneName,
    string? ExchangeTimezoneShortName,
    string? QuoteType,
    string? MarketState,
    decimal? RegularMarketPrice,
    decimal? RegularMarketPreviousClose,
    decimal? RegularMarketChange,
    decimal? RegularMarketChangePercent,
    long? MarketCap,
    string? Sector,
    string? Industry,
    string? Website,
    CacheMetadataDto Cache);
