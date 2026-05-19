namespace YFinance.NET.Models;

public sealed record HistoryMetadata(
    string Symbol,
    string? Currency,
    string? ExchangeName,
    string? ExchangeTimezoneName,
    string? InstrumentType,
    string? DataGranularity,
    decimal? RegularMarketPrice,
    int? PriceHint,
    long? GmtOffsetSeconds,
    IReadOnlyList<string> ValidRanges,
    IReadOnlyDictionary<string, object?> RawFields);