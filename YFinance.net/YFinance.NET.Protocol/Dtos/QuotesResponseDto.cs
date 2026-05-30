namespace YFinance.NET.Protocol.Dtos;

public sealed record QuotesResponseDto(
    IReadOnlyList<QuoteDto> Quotes,
    IReadOnlyList<string> MissingSymbols);
