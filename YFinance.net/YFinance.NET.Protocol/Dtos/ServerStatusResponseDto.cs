namespace YFinance.NET.Protocol.Dtos;

public sealed record ServerStatusResponseDto(
    string ServerVersion,
    int ProtocolVersion,
    string Mode,
    int ListenerPort,
    int ActiveConnectionCount,
    int MaxConcurrentClients,
    int CacheEntryCount,
    int? OwnerProcessId,
    string TracePath);
