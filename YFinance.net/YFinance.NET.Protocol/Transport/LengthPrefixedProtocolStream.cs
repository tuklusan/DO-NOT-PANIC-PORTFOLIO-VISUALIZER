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
using System.Buffers;
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
        byte[] prefix = new byte[ProtocolConstants.LengthPrefixBytes];
        bool prefixRead = await ReadExactIntoAsync(stream, prefix, allowEndAtStart: true, cancellationToken).ConfigureAwait(false);
        if (!prefixRead)
            return null;

        int length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (length < 0 || length > ProtocolConstants.MaxMessageBytes)
            throw new InvalidOperationException($"Invalid message length {length}.");
        if (length == 0)
            return Array.Empty<byte>();

        byte[] payload = new byte[length];
        await ReadExactIntoAsync(stream, payload, allowEndAtStart: false, cancellationToken).ConfigureAwait(false);
        return payload;
    }

    public static async Task<PooledProtocolPayload?> ReadPooledAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] prefix = ArrayPool<byte>.Shared.Rent(ProtocolConstants.LengthPrefixBytes);
        try
        {
            bool prefixRead = await ReadExactIntoAsync(stream, prefix.AsMemory(0, ProtocolConstants.LengthPrefixBytes), allowEndAtStart: true, cancellationToken).ConfigureAwait(false);
            if (!prefixRead)
                return null;

            int length = BinaryPrimitives.ReadInt32BigEndian(prefix.AsSpan(0, ProtocolConstants.LengthPrefixBytes));
            if (length < 0 || length > ProtocolConstants.MaxMessageBytes)
                throw new InvalidOperationException($"Invalid message length {length}.");
            if (length == 0)
                return PooledProtocolPayload.Empty();

            byte[] payload = ArrayPool<byte>.Shared.Rent(length);
            bool payloadTransferred = false;
            try
            {
                await ReadExactIntoAsync(stream, payload.AsMemory(0, length), allowEndAtStart: false, cancellationToken).ConfigureAwait(false);
                PooledProtocolPayload pooledPayload = new(payload, length);
                payloadTransferred = true;
                return pooledPayload;
            }
            finally
            {
                if (!payloadTransferred)
                    ArrayPool<byte>.Shared.Return(payload, clearArray: true);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(prefix, clearArray: true);
        }
    }

    private static async Task<bool> ReadExactIntoAsync(Stream stream, Memory<byte> buffer, bool allowEndAtStart, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (allowEndAtStart && offset == 0)
                    return false;

                throw new EndOfStreamException("Unexpected end of stream while reading framed protocol payload.");
            }

            offset += read;
        }

        return true;
    }
}

/// <summary>
/// Owns a protocol payload buffer rented from <see cref="ArrayPool{T}"/>.
/// Callers must dispose the payload exactly as they would close a stream; failing
/// to do so keeps the rented array unavailable for reuse until process exit.
/// </summary>
public sealed class PooledProtocolPayload : IDisposable
{
    private byte[]? _buffer;
    private readonly int _length;
    private readonly bool _returnToPool;

    internal PooledProtocolPayload(byte[] buffer, int length, bool returnToPool = true)
    {
        _buffer = buffer;
        _length = length;
        _returnToPool = returnToPool;
    }

    internal static PooledProtocolPayload Empty()
        => new(Array.Empty<byte>(), 0, returnToPool: false);

    public ReadOnlyMemory<byte> Memory
    {
        get
        {
            ObjectDisposedException.ThrowIf(_buffer is null, this);
            return _buffer.AsMemory(0, _length);
        }
    }

    public byte[] ToArray()
        => Memory.ToArray();

    public void Dispose()
    {
        byte[]? buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null && _returnToPool)
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
    }
}
