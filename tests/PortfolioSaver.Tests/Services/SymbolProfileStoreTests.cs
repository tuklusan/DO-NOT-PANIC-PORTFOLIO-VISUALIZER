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
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Services;
using Xunit;

namespace PortfolioSaver.Tests.Services;

public sealed class SymbolProfileStoreTests
{
    [Fact]
    public async Task LoadAsync_ReturnsSameNormalizedProfilesAsLoad()
    {
        string root = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        string storagePath = Path.Combine(root, "symbol-profiles.json");
        try
        {
            SymbolProfileStore store = new(storagePath);
            store.Save(
            [
                new SymbolProfile { Symbol = " voo ", CanonicalSymbol = " voo ", DisplayName = "Vanguard S&P 500 ETF" },
                new SymbolProfile { Symbol = "VOO", CanonicalSymbol = "VOO", DisplayName = "Latest wins" }
            ]);

            IReadOnlyDictionary<string, SymbolProfile> syncProfiles = store.Load();
            IReadOnlyDictionary<string, SymbolProfile> asyncProfiles = await store.LoadAsync();

            Assert.Equal(
                syncProfiles.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase),
                asyncProfiles.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase));
            Assert.Equal(syncProfiles["VOO"].CanonicalSymbol, asyncProfiles["VOO"].CanonicalSymbol);
            Assert.Equal("Latest wins", asyncProfiles["VOO"].DisplayName);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_PreCanceledTokenStopsBeforeFileRead()
    {
        string storagePath = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"), "symbol-profiles.json");
        SymbolProfileStore store = new(storagePath);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LoadAsync(cts.Token));
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmptyDictionaryForMalformedJson()
    {
        string root = Path.Combine(Path.GetTempPath(), "PortfolioSaver.Tests", Guid.NewGuid().ToString("N"));
        string storagePath = Path.Combine(root, "symbol-profiles.json");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(storagePath, "{ definitely-not-json");

            SymbolProfileStore store = new(storagePath);

            IReadOnlyDictionary<string, SymbolProfile> profiles = await store.LoadAsync();

            Assert.Empty(profiles);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

}
