namespace YFinance.NET.Models;

public sealed record HistoricalBar(
    DateTimeOffset Timestamp,
    decimal? Open,
    decimal? High,
    decimal? Low,
    decimal? Close,
    long? Volume);
