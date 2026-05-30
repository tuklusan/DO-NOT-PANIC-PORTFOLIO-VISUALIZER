namespace YFinance.NET.Protocol.Messages;

public sealed record ProtocolEvent<TPayload> : ProtocolEnvelope
{
    public ProtocolEvent()
    {
        MessageType = Constants.ProtocolMessageTypes.Event;
    }

    public string EventType { get; init; } = string.Empty;
    public TPayload? Payload { get; init; }
}
