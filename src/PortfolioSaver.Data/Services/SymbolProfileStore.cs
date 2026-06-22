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
using PortfolioSaver.Core.Models;
using PortfolioSaver.Core.Services;

namespace PortfolioSaver.Data.Services;

public sealed class SymbolProfileStore
{
    private readonly string _storagePath;

    public SymbolProfileStore(string storagePath)
    {
        _storagePath = storagePath;
    }

    public IReadOnlyDictionary<string, SymbolProfile> Load()
    {
        if (!File.Exists(_storagePath))
            return new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase);

        try
        {
            List<SymbolProfile>? profiles = JsonSerializer.Deserialize<List<SymbolProfile>>(File.ReadAllText(_storagePath));
            return NormalizeProfiles(profiles);
        }
        catch
        {
            return new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task<IReadOnlyDictionary<string, SymbolProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_storagePath))
            return new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await using FileStream stream = new(_storagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, useAsync: true);
            List<SymbolProfile>? profiles = await JsonSerializer.DeserializeAsync<List<SymbolProfile>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return NormalizeProfiles(profiles);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new Dictionary<string, SymbolProfile>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Save(IEnumerable<SymbolProfile> profiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storagePath) ?? ".");

        List<SymbolProfile> normalized = profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Symbol))
            .GroupBy(profile => SymbolProfileHeuristics.Normalize(profile.Symbol), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                SymbolProfile profile = group.Last();
                profile.Symbol = SymbolProfileHeuristics.Normalize(profile.Symbol);
                profile.CanonicalSymbol = string.IsNullOrWhiteSpace(profile.CanonicalSymbol)
                    ? profile.Symbol
                    : SymbolProfileHeuristics.Normalize(profile.CanonicalSymbol);
                return profile;
            })
            .OrderBy(profile => profile.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string json = JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storagePath, json);
    }

    private static IReadOnlyDictionary<string, SymbolProfile> NormalizeProfiles(IEnumerable<SymbolProfile>? profiles)
        => (profiles ?? [])
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Symbol))
            .GroupBy(profile => SymbolProfileHeuristics.Normalize(profile.Symbol), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToDictionary(profile => SymbolProfileHeuristics.Normalize(profile.Symbol), StringComparer.OrdinalIgnoreCase);
}
