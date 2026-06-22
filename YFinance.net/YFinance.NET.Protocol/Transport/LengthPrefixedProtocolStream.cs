// ============================================================================
// Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
// Proprietary rights reserved except as expressly licensed herein.
//
// DO NOT PANIC PORTFOLIO VIEWER
// This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
// personal, educational, or hobbyist use only. Commercial exploitation,
// corporate internal operations, or AI model training are strictly forbidden.
//
// ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
// which is licensed under the Apache License, Version 2.0. A copy of the Apache
// License is provided within the distribution environment.
//
// FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
// It does not provide financial, investment, legal, or tax advice. All data
// calculation and scraping outputs are provided 'AS IS' with zero guarantee
// of real-time accuracy or upstream availability.
//
// This file is subject to the terms and conditions defined in the LICENSE
// file located in the root directory of this source code repository.
// Removal or modification of this legal notice constitutes copyright infringement.
// ============================================================================
using System.Buffers.Binary;
using YFinance.NET.Protocol.Constants;

namespace YFinance.NET.Protocol.Transport;

public static class LengthPrefixedProtocolStream
{
    public static async Task WriteAsync(Stream stream, byte[] payload, CancellationToken cancellationToken = default)
    {
        if (payload.Length > ProtocolConstants.MaxMessageBytes)
            throw new InvalidOperationException($"Payload exceeds max message size of {ProtocolConstants.MaxMessageBytes} bytes.");

        byte[] prefix = new byte[ProtocolConstants.LengthPrefixBytes];
        BinaryPrimitives.WriteInt32BigEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[]? prefix = await ReadExactAsync(stream, ProtocolConstants.LengthPrefixBytes, cancellationToken).ConfigureAwait(false);
        if (prefix is null)
            return null;

        int length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (length < 0 || length > ProtocolConstants.MaxMessageBytes)
            throw new InvalidOperationException($"Invalid message length {length}.");

        return await ReadExactAsync(stream, length, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadExactAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return offset == 0 ? null : throw new EndOfStreamException("Unexpected end of stream while reading framed protocol payload.");
            offset += read;
        }
        return buffer;
    }
}
