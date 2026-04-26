using System.Text.Json;
using PortfolioSaver.Core.Models;
using PortfolioSaver.Data.Interfaces;

namespace PortfolioSaver.Data.Services;

public sealed class QuoteCacheService : IQuoteCacheService
{
    private readonly string _cachePath;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public QuoteCacheService(string cachePath)
    {
        _cachePath = cachePath;
    }

    public async Task SaveAsync(IEnumerable<QuoteSnapshot> quotes, CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(_cachePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using FileStream stream = File.Create(_cachePath);
        await JsonSerializer.SerializeAsync(stream, quotes, JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<QuoteSnapshot>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_cachePath))
            return [];

        await using FileStream stream = File.OpenRead(_cachePath);
        IReadOnlyList<QuoteSnapshot>? quotes = await JsonSerializer.DeserializeAsync<List<QuoteSnapshot>>(stream, JsonOptions, cancellationToken);
        return quotes ?? [];
    }

    public IReadOnlyList<QuoteSnapshot> LoadCached()
    {
        if (!File.Exists(_cachePath))
            return [];

        try
        {
            string json = File.ReadAllText(_cachePath);
            return JsonSerializer.Deserialize<List<QuoteSnapshot>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
