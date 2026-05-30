namespace YFinance.NET.Protocol.Dtos;

public sealed record CacheMetadataDto(string Source, int AgeSeconds, bool Stale);
