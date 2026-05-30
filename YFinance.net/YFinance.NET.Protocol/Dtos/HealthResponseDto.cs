namespace YFinance.NET.Protocol.Dtos;

public sealed record HealthResponseDto(
    string Status,
    double UptimeSeconds,
    int ActiveConnectionCount,
    int CacheEntryCount,
    string Mode);
