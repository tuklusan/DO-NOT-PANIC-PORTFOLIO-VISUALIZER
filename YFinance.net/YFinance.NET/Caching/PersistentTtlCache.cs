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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YFinance.NET.Caching;

public sealed class PersistentTtlCache<TValue>
{
    private readonly string _rootPath;
    private readonly JsonSerializerOptions _serializerOptions;

    public PersistentTtlCache(string rootPath, JsonSerializerOptions? serializerOptions = null)
    {
        _rootPath = rootPath;
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<TValue?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        string path = GetPath(key);
        if (!File.Exists(path))
        {
            return default;
        }

        await using FileStream stream = File.OpenRead(path);
        CacheEnvelope<TValue>? envelope = await JsonSerializer.DeserializeAsync<CacheEnvelope<TValue>>(stream, _serializerOptions, cancellationToken).ConfigureAwait(false);
        if (envelope is null || envelope.ExpiresUtc <= DateTimeOffset.UtcNow)
        {
            TryDelete(path);
            return default;
        }

        return envelope.Value;
    }

    public async Task SetAsync(string key, TValue value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        string path = GetPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        CacheEnvelope<TValue> envelope = new(value, DateTimeOffset.UtcNow.Add(ttl));
        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, envelope, _serializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public static string BuildKey(params object?[] parts)
        => string.Join(':', parts.Select(static part => part?.ToString() ?? string.Empty));

    private string GetPath(string key)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        string fileName = Convert.ToHexString(bytes).ToLowerInvariant();
        return Path.Combine(_rootPath, fileName + ".json");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed record CacheEnvelope<T>(T Value, DateTimeOffset ExpiresUtc);
}
