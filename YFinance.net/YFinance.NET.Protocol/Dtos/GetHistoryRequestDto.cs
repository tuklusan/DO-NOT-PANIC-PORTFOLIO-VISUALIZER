namespace YFinance.NET.Protocol.Dtos;

public sealed record GetHistoryRequestDto(string Symbol, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string Interval);
