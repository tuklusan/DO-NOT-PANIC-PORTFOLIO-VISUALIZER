namespace YFinance.NET.Models;

public sealed record HistoryMetadata(
    string Symbol,
    string? Currency,
    string? ExchangeName,
    string? ExchangeTimezoneName,
    string? InstrumentType,
    string? DataGranularity,
    decimal? RegularMarketPrice,
    DateTimeOffset? RegularMarketTimeUtc,
    int? PriceHint,
    long? GmtOffsetSeconds,
    CurrentTradingPeriods? CurrentTradingPeriod,
    IReadOnlyList<string> ValidRanges,
    IReadOnlyDictionary<string, object?> RawFields);
