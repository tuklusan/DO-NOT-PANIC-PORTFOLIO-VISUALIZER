namespace YFinance.NET.Protocol.Dtos;

public sealed record HistoryBarDto(DateTimeOffset TimestampUtc, decimal? Open, decimal? High, decimal? Low, decimal? Close, long? Volume);
