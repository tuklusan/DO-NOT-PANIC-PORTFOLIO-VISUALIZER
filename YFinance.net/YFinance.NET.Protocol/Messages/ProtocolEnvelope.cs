namespace YFinance.NET.Protocol.Messages;

public abstract record ProtocolEnvelope
{
    public int ProtocolVersion { get; set; } = Constants.ProtocolConstants.Version;
    public string MessageType { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string PayloadChecksum { get; set; } = string.Empty;
}
