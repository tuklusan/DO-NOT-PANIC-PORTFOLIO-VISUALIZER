// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VISUALIZER
// This file is governed by the SANYALnet Labs Non-Commercial License in the
// root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
// for AI/ML model training are prohibited unless separately authorized.
//
// Attribution is required: "Based on original work by Supratim Sanyal of
// SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
// patent, trademark, and governing-law provisions.
// ============================================================================
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
