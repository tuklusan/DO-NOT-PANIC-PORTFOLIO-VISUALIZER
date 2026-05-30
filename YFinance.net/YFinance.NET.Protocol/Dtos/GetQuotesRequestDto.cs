namespace YFinance.NET.Protocol.Dtos;

public sealed record GetQuotesRequestDto(IReadOnlyList<string> Symbols);
