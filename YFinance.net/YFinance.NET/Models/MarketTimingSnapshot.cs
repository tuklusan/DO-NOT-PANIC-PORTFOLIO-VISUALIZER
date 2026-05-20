namespace YFinance.NET.Models;

public sealed record MarketTimingSnapshot(
    string Symbol,
    string? ExchangeName,
    string? ExchangeTimezoneName,
    string? InstrumentType,
    DateTimeOffset? RegularMarketTimeUtc,
    long? GmtOffsetSeconds,
    CurrentTradingPeriods? CurrentTradingPeriod,
    DateOnly ExchangeLocalDate,
    DateTimeOffset FetchedUtc,
    IReadOnlyDictionary<string, object?> RawFields);
