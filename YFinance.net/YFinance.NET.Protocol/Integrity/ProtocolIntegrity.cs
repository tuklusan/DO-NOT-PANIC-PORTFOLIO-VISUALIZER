using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YFinance.NET.Protocol.Messages;
using YFinance.NET.Protocol.Transport;

namespace YFinance.NET.Protocol.Integrity;

public static class ProtocolIntegrity
{
    public static string ComputePayloadChecksum<TPayload>(TPayload? payload)
    {
        byte[] payloadBytes = payload switch
        {
            null => Encoding.UTF8.GetBytes("null"),
            JsonElement element => Encoding.UTF8.GetBytes(element.GetRawText()),
            _ => ProtocolJson.Serialize(payload)
        };
        byte[] hash = SHA256.HashData(payloadBytes);
        return Convert.ToHexString(hash);
    }

    public static void Stamp<TPayload>(ProtocolEnvelope envelope, TPayload? payload)
    {
        envelope.Timestamp = DateTimeOffset.Now;
        envelope.PayloadChecksum = ComputePayloadChecksum(payload);
    }

    public static bool Verify<TPayload>(ProtocolEnvelope envelope, TPayload? payload)
        => string.Equals(envelope.PayloadChecksum, ComputePayloadChecksum(payload), StringComparison.OrdinalIgnoreCase);
}
