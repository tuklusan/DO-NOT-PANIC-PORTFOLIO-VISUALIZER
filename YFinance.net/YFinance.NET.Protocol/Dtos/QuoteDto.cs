namespace YFinance.NET.Protocol.Dtos;

public sealed record QuoteDto(
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
    decimal? RegularMarketOpen,
    decimal? RegularMarketDayHigh,
    decimal? RegularMarketDayLow,
    decimal? RegularMarketChange,
    decimal? RegularMarketChangePercent,
    long? MarketCap,
    long? RegularMarketVolume,
    DateTimeOffset FetchTimestampUtc,
    CacheMetadataDto Cache);
