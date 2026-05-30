namespace YFinance.NET.Protocol.Messages;

public sealed record ProtocolRequest<TPayload> : ProtocolEnvelope
{
    public ProtocolRequest()
    {
        MessageType = Constants.ProtocolMessageTypes.Request;
    }

    public string RequestId { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public TPayload? Payload { get; init; }
}
