using YFinance.NET.Protocol.Errors;

namespace YFinance.NET.Protocol.Messages;

public sealed record ProtocolResponse<TPayload> : ProtocolEnvelope
{
    public ProtocolResponse()
    {
        MessageType = Constants.ProtocolMessageTypes.Response;
    }

    public string RequestId { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string Status { get; init; } = Constants.ProtocolResponseStatuses.Ok;
    public TPayload? Payload { get; init; }
    public ProtocolError? Error { get; init; }
}
