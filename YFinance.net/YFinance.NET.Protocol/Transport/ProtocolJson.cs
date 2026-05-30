using System.Text.Json;
using System.Text.Json.Serialization;

namespace YFinance.NET.Protocol.Transport;

public static class ProtocolJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    public static byte[] Serialize<T>(T value)
        => JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);

    public static T? Deserialize<T>(ReadOnlySpan<byte> payload)
        => JsonSerializer.Deserialize<T>(payload, SerializerOptions);

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
