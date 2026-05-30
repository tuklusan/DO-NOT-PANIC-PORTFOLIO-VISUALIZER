namespace YFinance.NET.Protocol.Dtos;

public sealed record HistoryResponseDto(
    string Symbol,
    IReadOnlyList<HistoryBarDto> Bars,
    HistoryMetadataDto? Metadata,
    CacheMetadataDto Cache);
