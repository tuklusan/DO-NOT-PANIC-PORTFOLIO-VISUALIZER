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
}
