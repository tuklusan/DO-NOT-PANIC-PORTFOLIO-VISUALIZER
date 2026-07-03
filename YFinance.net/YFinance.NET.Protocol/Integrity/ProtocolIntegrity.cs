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
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YFinance.NET.Protocol.Messages;
using YFinance.NET.Protocol.Transport;

namespace YFinance.NET.Protocol.Integrity;

public static class ProtocolIntegrity
{
    private const int FastChecksumHexLength = 16;
    private const int LegacySha256ChecksumHexLength = 64;

    public static string ComputePayloadChecksum<TPayload>(TPayload? payload)
    {
        byte[] payloadBytes = SerializePayload(payload);
        ulong hash = ComputeFnv1A64(payloadBytes);
        return hash.ToString("X16", CultureInfo.InvariantCulture);
    }

    private static byte[] SerializePayload<TPayload>(TPayload? payload)
        => payload switch
        {
            null => Encoding.UTF8.GetBytes("null"),
            JsonElement element => Encoding.UTF8.GetBytes(element.GetRawText()),
            _ => ProtocolJson.Serialize(payload)
        };

    private static ulong ComputeFnv1A64(ReadOnlySpan<byte> payloadBytes)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        unchecked
        {
            ulong hash = offsetBasis;
            foreach (byte value in payloadBytes)
            {
                hash ^= value;
                hash *= prime;
            }

            return hash;
        }
    }

    public static void Stamp<TPayload>(ProtocolEnvelope envelope, TPayload? payload)
    {
        envelope.Timestamp = DateTimeOffset.Now;
        envelope.PayloadChecksum = ComputePayloadChecksum(payload);
    }

    public static bool Verify<TPayload>(ProtocolEnvelope envelope, TPayload? payload)
    {
        string payloadChecksum = envelope.PayloadChecksum;
        if (string.IsNullOrWhiteSpace(payloadChecksum))
            return false;

        byte[] payloadBytes = SerializePayload(payload);
        if (payloadChecksum.Length == FastChecksumHexLength)
        {
            string fastChecksum = ComputeFnv1A64(payloadBytes).ToString("X16", CultureInfo.InvariantCulture);
            if (string.Equals(payloadChecksum, fastChecksum, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (payloadChecksum.Length != LegacySha256ChecksumHexLength)
            return false;

        if (!IsHex(payloadChecksum))
            return false;

        // This checksum detects accidental transport corruption, not adversarial tampering.
        // The product ships the client and owned server as one bundle; mixed-version
        // deployments are unsupported except that new code accepts prior SHA-256
        // envelopes to avoid rejecting an older in-flight message during startup/shutdown.
        return string.Equals(payloadChecksum, Convert.ToHexString(SHA256.HashData(payloadBytes)), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value)
        {
            bool isHexDigit = c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f';
            if (!isHexDigit)
                return false;
        }

        return true;
    }
}
