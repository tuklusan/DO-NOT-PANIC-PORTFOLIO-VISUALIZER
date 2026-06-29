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
