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
            return (profiles ?? [])
                .Where(profile => !string.IsNullOrWhiteSpace(profile.Symbol))
                .GroupBy(profile => SymbolProfileHeuristics.Normalize(profile.Symbol), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToDictionary(profile => SymbolProfileHeuristics.Normalize(profile.Symbol), StringComparer.OrdinalIgnoreCase);
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
                profile.SupportedQuoteSources = profile.SupportedQuoteSources
                    .Distinct()
                    .OrderBy(kind => (int)kind)
                    .ToList();
                return profile;
            })
            .OrderBy(profile => profile.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string json = JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storagePath, json);
    }
}
