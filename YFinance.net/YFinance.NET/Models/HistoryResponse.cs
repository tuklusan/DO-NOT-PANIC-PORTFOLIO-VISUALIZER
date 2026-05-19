namespace YFinance.NET.Models;

public sealed record HistoryResponse(
    string Symbol,
    IReadOnlyList<HistoricalBar> Bars,
    HistoryMetadata? Metadata);