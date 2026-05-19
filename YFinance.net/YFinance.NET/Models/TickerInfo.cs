namespace YFinance.NET.Models;

public sealed record TickerInfo(
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
    decimal? FiftyTwoWeekLow,
    decimal? FiftyTwoWeekHigh,
    decimal? FiftyDayAverage,
    decimal? TwoHundredDayAverage,
    long? RegularMarketVolume,
    long? AverageVolume,
    long? AverageVolume10Day,
    long? SharesOutstanding,
    long? MarketCap,
    decimal? TrailingPe,
    decimal? ForwardPe,
    decimal? DividendYield,
    string? Sector,
    string? Industry,
    string? LongBusinessSummary,
    string? Website,
    IReadOnlyDictionary<string, object?> FlatFields)
{
    public decimal? ComputedChangePercent =>
        RegularMarketPrice.HasValue && RegularMarketPreviousClose is not null && RegularMarketPreviousClose.Value != 0m
            ? ((RegularMarketPrice.Value - RegularMarketPreviousClose.Value) / RegularMarketPreviousClose.Value) * 100m
            : RegularMarketChangePercent;
}