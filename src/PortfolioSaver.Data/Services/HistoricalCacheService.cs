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
using System.Text.Json;
using PortfolioSaver.Core.Constants;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;

namespace PortfolioSaver.Data.Services;

public sealed class HistoricalCacheService : IHistoricalCacheService
{
    private readonly string _rootFolder;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

    public HistoricalCacheService(string? rootFolder = null)
    {
        string configuredRoot = string.IsNullOrWhiteSpace(rootFolder)
            ? Defaults.GetHistoricalCacheFolder()
            : Environment.ExpandEnvironmentVariables(rootFolder);

        _rootFolder = configuredRoot;

        Directory.CreateDirectory(_rootFolder);
    }

    internal Action? PurgeStartedForTesting { get; set; }
    internal Action? PurgeIterationForTesting { get; set; }

    public async Task<TickerHistorySnapshot?> LoadAsync(string symbol, CancellationToken cancellationToken = default)
    {
        string path = GetPath(symbol);
        if (!File.Exists(path))
            return null;

        FileInfo info = new(path);
        if (DateTimeOffset.UtcNow - info.LastWriteTimeUtc > MaxAge)
        {
            TryDelete(path);
            return null;
        }

        if (info.Length == 0)
        {
            TryDelete(path);
            return null;
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<TickerHistorySnapshot>(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            TryDelete(path);
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task SaveAsync(TickerHistorySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        string path = GetPath(snapshot.Symbol);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, snapshot, cancellationToken: cancellationToken);
    }

    public Task PurgeExpiredAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => PurgeExpired(cancellationToken), cancellationToken);

    private void PurgeExpired(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_rootFolder))
            return;

        PurgeStartedForTesting?.Invoke();
        foreach (string file in Directory.EnumerateFiles(_rootFolder, "*.json", SearchOption.TopDirectoryOnly))
        {
            PurgeIterationForTesting?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo info = new(file);
            if (DateTimeOffset.UtcNow - info.LastWriteTimeUtc > MaxAge)
                TryDelete(file);
        }
    }

    private string GetPath(string symbol)
    {
        string safe = string.Concat(symbol.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        if (string.IsNullOrWhiteSpace(safe))
            safe = "unknown";

        return Path.Combine(_rootFolder, $"{safe.ToUpperInvariant()}.json");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { }
    }
}
