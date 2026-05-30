namespace YFinance.NET.Protocol.Dtos;

public sealed record HelloResponseDto(
    string ServerVersion,
    int ProtocolVersion,
    IReadOnlyList<string> Capabilities,
    int ListenerPort,
    string Mode,
    int ActiveConnectionCount);
