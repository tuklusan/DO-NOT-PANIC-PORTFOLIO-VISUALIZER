using System.Text.Json;

namespace YFinance.NET.Models;

public sealed record QuoteSnapshot(
    string Symbol,
    string? ShortName,
    string? LongName,
    string? Currency,
    string? Exchange,
    string? QuoteType,
    decimal? RegularMarketPrice,
    decimal? RegularMarketPreviousClose,
    decimal? RegularMarketChangePercent,
    long? MarketCap,
    JsonElement Raw)
{
    public decimal? ComputedChangePercent =>
        RegularMarketPrice.HasValue && RegularMarketPreviousClose.HasValue && RegularMarketPreviousClose.Value != 0m
            ? ((RegularMarketPrice.Value - RegularMarketPreviousClose.Value) / RegularMarketPreviousClose.Value) * 100m
            : RegularMarketChangePercent;
}
