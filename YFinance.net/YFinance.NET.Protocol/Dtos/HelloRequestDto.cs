namespace YFinance.NET.Protocol.Dtos;

public sealed record HelloRequestDto(
    string ClientType,
    string ClientVersion,
    string MachineHash,
    bool OwnedMode,
    int? OwnerProcessId);
